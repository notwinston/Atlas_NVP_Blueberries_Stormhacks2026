using System;
using Atlas.Rendering;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Spaces.AlignedCells
{
    // Master Plan §3.5 "a post at every tile corner", §4.7 "a post at every interior corner": one merged mesh.
    public static class PostMesh
    {
        public const int Sides = TileChart.PostArcSegments * 4;
        const int RingVerts = Sides + 1;                 // seam duplicated
        const int PostVerts = 2 * RingVerts + 1;
        const int PostIndices = Sides * 9;               // 2 side + 1 cap triangles per side

        public static void Build(CellGrid grid, TilePalette palette, Mesh target)
        {
            int posts = grid.InteriorCornerCount;
            var verts = new TileVertex[Math.Max(posts, 1) * PostVerts];
            var idx = new ushort[Math.Max(posts, 1) * PostIndices];
            int vc = 0, ic = 0;
            float r = (float)TileChart.PostRadius, h = TileChart.PostHeight;
            for (int X = 1; X < grid.CountX; X++)
                for (int Z = 1; Z < grid.CountZ; Z++)
                {
                    double2 c = grid.Corner(X, Z);
                    int v0 = vc;
                    for (int j = 0; j <= Sides; j++)
                    {
                        double phi = 2 * Math.PI * j / Sides;
                        float x = (float)(c.x + r * Math.Cos(phi)), z = (float)(c.y + r * Math.Sin(phi));
                        verts[vc++] = new TileVertex { Position = new float3(x, 0, z), Color = Ao(TileChart.AoMin), Uv = palette.Post };
                    }
                    for (int j = 0; j <= Sides; j++)
                    {
                        var b = verts[v0 + j].Position;
                        verts[vc++] = new TileVertex { Position = new float3(b.x, h, b.z), Color = Ao(1), Uv = palette.Post };
                    }
                    int centre = vc;
                    verts[vc++] = new TileVertex { Position = new float3((float)c.x, h, (float)c.y), Color = Ao(1), Uv = palette.Post };
                    for (int j = 0; j < Sides; j++)
                    {
                        int b0 = v0 + j, b1 = v0 + j + 1, t0 = v0 + RingVerts + j, t1 = t0 + 1;
                        Tri(idx, ref ic, b0, t0, t1);
                        Tri(idx, ref ic, b0, t1, b1);
                        Tri(idx, ref ic, centre, t1, t0);
                    }
                }
            if (posts == 0) { vc = 0; ic = 0; }
            Upload(verts, vc, idx, ic, target);
        }

        static Color32 Ao(double v) { byte b = (byte)math.round(math.saturate((float)v) * 255f); return new Color32(b, b, b, 255); }
        static void Tri(ushort[] idx, ref int ic, int a, int b, int c) { idx[ic++] = (ushort)a; idx[ic++] = (ushort)b; idx[ic++] = (ushort)c; }

        static void Upload(TileVertex[] verts, int vc, ushort[] idx, int ic, Mesh target)
        {
            float3 mn = vc > 0 ? verts[0].Position : float3.zero, mx = mn;
            for (int i = 1; i < vc; i++) { mn = math.min(mn, verts[i].Position); mx = math.max(mx, verts[i].Position); }
            var bounds = new Bounds((mn + mx) * 0.5f, mx - mn);
            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
            var arr = Mesh.AllocateWritableMeshData(1);
            var md = arr[0];
            md.SetVertexBufferParams(vc, TileVertex.Descriptors);
            md.SetIndexBufferParams(ic, IndexFormat.UInt16);
            NativeArray<TileVertex>.Copy(verts, md.GetVertexData<TileVertex>(), vc);
            NativeArray<ushort>.Copy(idx, md.GetIndexData<ushort>(), ic);
            md.subMeshCount = 1;
            md.SetSubMesh(0, new SubMeshDescriptor(0, ic) { bounds = bounds, vertexCount = vc }, flags);
            Mesh.ApplyAndDisposeWritableMeshData(arr, target, flags);
            target.bounds = bounds;
        }
    }
}
