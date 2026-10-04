using System;
using Atlas.Kernel;
using Atlas.Rendering;
using Unity.Mathematics;

namespace Atlas.Spaces.AlignedCells
{
    // Master Plan §3.5 "a post at every tile corner", §4.7 "a post at every interior corner": one merged mesh.
    // Engine-free: written into the look's AtlasMeshBuilder (lane A contract §10.2) and uploaded with AtlasMesh.
    public static class PostMesh
    {
        public const int Sides = TileChart.PostArcSegments * 4;
        const int RingVerts = Sides + 1;                 // seam duplicated
        const int PostVerts = 2 * RingVerts + 1;
        const int PostIndices = Sides * 9;               // 2 side + 1 cap triangles per side

        public static int TrianglesPerPost => PostIndices / 3;

        // Appends the posts of the grid's interior corners, anchor frame, metres. Returns the triangles added.
        public static int Build(CellGrid grid, TilePalette palette, AtlasMeshBuilder into)
        {
            int posts = grid.InteriorCornerCount;
            into.Reserve(posts * PostVerts, posts * PostIndices);
            float r = (float)TileChart.PostRadius, h = TileChart.PostHeight;
            var uv1 = new float2(0.5f, 0.5f);            // no line crosses a post
            for (int X = 1; X < grid.CountX; X++)
                for (int Z = 1; Z < grid.CountZ; Z++)
                {
                    double2 c = grid.Corner(X, Z);
                    int v0 = into.VertexCount;
                    for (int j = 0; j <= Sides; j++)
                    {
                        double phi = 2 * Math.PI * j / Sides;
                        var n = new float3((float)Math.Cos(phi), 0, (float)Math.Sin(phi));
                        float x = (float)(c.x + r * Math.Cos(phi)), z = (float)(c.y + r * Math.Sin(phi));
                        into.AddVertex(new AtlasVertex { Position = new float3(x, 0, z), Normal = n, Color = Ao(TileChart.AoMin), Uv0 = palette.Post, Uv1 = uv1 });
                    }
                    for (int j = 0; j <= Sides; j++)
                    {
                        AtlasVertex b = into.Vertices[v0 + j];
                        into.AddVertex(new AtlasVertex { Position = new float3(b.Position.x, h, b.Position.z), Normal = b.Normal, Color = Ao(1), Uv0 = palette.Post, Uv1 = uv1 });
                    }
                    int centre = into.AddVertex(new AtlasVertex { Position = new float3((float)c.x, h, (float)c.y), Normal = new float3(0, 1, 0), Color = Ao(1), Uv0 = palette.Post, Uv1 = uv1 });
                    for (int j = 0; j < Sides; j++)
                    {
                        int b0 = v0 + j, b1 = v0 + j + 1, t0 = v0 + RingVerts + j, t1 = t0 + 1;
                        into.AddTriangle(b0, t0, t1);
                        into.AddTriangle(b0, t1, b1);
                        into.AddTriangle(centre, t1, t0);
                    }
                }
            return posts * TrianglesPerPost;
        }

        static uint Ao(double v) => AtlasVertex.PackColor((byte)math.round(math.saturate((float)v) * 255f), 0);
    }
}
