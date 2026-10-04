using System.Collections.Generic;
using Atlas.Rendering;
using UnityEngine;

namespace Atlas.World.Lint
{
    // Built B05 meshes per (lod, kerb mask): triangle count, wedge presence and CPU copies of the geometry.
    // Temporary Mesh objects are destroyed straight after the read-back.
    sealed class TileMeshCache
    {
        public sealed class Entry
        {
            public int Triangles;
            public bool AllWedges;
            public Vector3[] Vertices;
            public int[] Indices;
        }

        readonly double L;
        readonly TileMeshGenerator gen = new TileMeshGenerator(TilePalette.Provisional);
        readonly Dictionary<int, Entry> cache = new Dictionary<int, Entry>();

        public TileMeshCache(double L) { this.L = L; }

        public Entry Get(TileLod lod, int kerbMask)
        {
            int key = ((int)lod << 8) | kerbMask;
            if (cache.TryGetValue(key, out var e)) return e;
            var mesh = new Mesh();
            try
            {
                var lay = gen.Build(TileMeshSpec.Hyperbolic(5, L, lod, kerbMask, 0xF), mesh);
                e = new Entry { Triangles = lay.TriangleCount, AllWedges = true };
                for (int c = 0; c < 4; c++) if (lay.PostIndices(c).length <= 0) e.AllWedges = false;
                e.Vertices = mesh.vertices;
                e.Indices = mesh.triangles;
            }
            finally { Object.DestroyImmediate(mesh); }
            cache[key] = e;
            return e;
        }
    }
}
