using Atlas.Kernel;
using Atlas.Rendering;
using UnityEngine;

namespace Atlas.TileMeshes.Tests
{
    // What a tile mesh holds, read from the generator's arrays with no engine object, so the geometry tests run headless.
    // The member names are those of UnityEngine.Mesh.
    public sealed class TileMeshData
    {
        public Vector3[] vertices;
        public Color32[] colors32;   // r = AO, g = line weight
        public Vector2[] uv, uv2;    // palette coordinate, line coordinate
        int[] indices;

        public TileMeshLayout Build(TileMeshGenerator gen, in TileMeshSpec spec)
        {
            var lay = gen.Build(spec);
            AtlasVertex[] src = gen.Vertices;
            vertices = new Vector3[lay.VertexCount]; colors32 = new Color32[lay.VertexCount];
            uv = new Vector2[lay.VertexCount]; uv2 = new Vector2[lay.VertexCount];
            for (int i = 0; i < lay.VertexCount; i++)
            {
                uint c = src[i].Color;
                vertices[i] = new Vector3(src[i].Position.x, src[i].Position.y, src[i].Position.z);
                colors32[i] = new Color32((byte)c, (byte)(c >> 8), (byte)(c >> 16), (byte)(c >> 24));
                uv[i] = new Vector2(src[i].Uv0.x, src[i].Uv0.y);
                uv2[i] = new Vector2(src[i].Uv1.x, src[i].Uv1.y);
            }
            indices = new int[lay.IndexCount];
            for (int i = 0; i < lay.IndexCount; i++) indices[i] = gen.Indices[i];
            return lay;
        }

        public int[] GetIndices(int submesh) => indices;
        public int GetIndexCount(int submesh) => indices.Length;
    }
}
