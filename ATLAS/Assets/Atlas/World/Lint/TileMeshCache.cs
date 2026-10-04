using System.Collections.Generic;
using Atlas.Rendering;
using UnityEngine;

namespace Atlas.World.Lint
{
    // The meshes the phone draws, per (lod, content key): the B05 shell with the tile's kerbs and wedges and the archetype's
    // props (WorldTileMeshBuilder, P1-B07). Triangle counts, wedge presence and CPU copies of the geometry; no engine object.
    sealed class TileMeshCache
    {
        public sealed class Entry
        {
            public int Triangles;
            public int PropTriangles;
            public double PropLongestEdge;   // metres, curved metric
            public bool AllWedges;
            public Vector3[] Vertices;
            public int[] Indices;
        }

        readonly double L;
        readonly WorldTileMeshBuilder builder = new WorldTileMeshBuilder();
        readonly Dictionary<int, Entry> cache = new Dictionary<int, Entry>();

        public TileMeshCache(double L) { this.L = L; }

        public Entry Get(TileLod lod, int kerbMask) => Get(lod, new WorldTileKey(Archetype.Plaza, kerbMask));

        public Entry Get(TileLod lod, in WorldTileKey key)
        {
            int code = ((int)lod << 16) | key.Code;
            if (cache.TryGetValue(code, out var e)) return e;
            var info = builder.Build(5, L, lod, key);
            var data = builder.Data;
            e = new Entry { Triangles = info.Triangles, PropTriangles = info.PropTriangles, AllWedges = true };
            for (int c = 0; c < 4; c++) if (info.Shell.PostIndices(c).length <= 0) e.AllWedges = false;
            e.PropLongestEdge = WorldPropMeshBuilder.LongestCurvedEdge(data, info.PropFirstIndex, L);
            e.Vertices = new Vector3[data.VertexCount];
            for (int i = 0; i < e.Vertices.Length; i++)
            {
                var p = data.Vertices[i].Position;
                e.Vertices[i] = new Vector3(p.x, p.y, p.z);
            }
            e.Indices = new int[data.IndexCount];
            System.Array.Copy(data.Indices, e.Indices, data.IndexCount);
            cache[code] = e;
            return e;
        }
    }
}
