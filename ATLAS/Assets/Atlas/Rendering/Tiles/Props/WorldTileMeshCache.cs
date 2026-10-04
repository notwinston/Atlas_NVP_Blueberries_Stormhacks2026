// P1-B07: the uploaded meshes of the generated world, one per (level of detail, WorldTileKey), built on first use. The key is
// finite (WorldTileKey.CodeCount x 3 levels; a world uses fewer than a hundred), so the cache is bounded. A Mesh object is
// kept for the cache's life and rebuilt in place when L changes, so a renderer may hold on to it.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Atlas.Rendering
{
    public sealed class WorldTileMeshCache : IDisposable
    {
        /// <summary>While L ramps, at most this many meshes are rebuilt per frame; the rest keep the last L for a frame or two.</summary>
        public const int MaxStaleRebuildsPerFrame = 8;

        sealed class Entry { public Mesh Mesh; public double L; public int Triangles; }

        readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>(128);
        readonly WorldTileMeshBuilder builder = new WorldTileMeshBuilder();
        int rebuildsLeft = MaxStaleRebuildsPerFrame;

        public WorldTileMeshCache(int q, double lengthScale)
        {
            if (q != 5 && q != 6) throw new ArgumentOutOfRangeException(nameof(q));
            Q = q;
            SetLengthScale(lengthScale);
        }

        public int Q { get; }
        public double L { get; private set; }
        public int Count => entries.Count;
        public long Builds { get; private set; }

        public void SetLengthScale(double lengthScale)
        {
            if (double.IsNaN(lengthScale) || double.IsInfinity(lengthScale) || lengthScale < 0.4) throw new ArgumentOutOfRangeException(nameof(lengthScale), "L must be finite and at least 0.4 m");
            L = lengthScale;
        }

        /// <summary>Once per frame, before the Gets: renews the allowance of stale rebuilds.</summary>
        public void BeginFrame() => rebuildsLeft = MaxStaleRebuildsPerFrame;

        static int LodIndex(TileLod lod) => lod == TileLod.Near ? 0 : lod == TileLod.Mid ? 1 : 2;

        /// <summary>The mesh of a tile with this content at this level. Allocates only the first time a key is asked for.</summary>
        public Mesh Get(TileLod lod, in WorldTileKey key, out int triangles)
        {
            int code = (key.Code << 2) | LodIndex(lod);
            if (!entries.TryGetValue(code, out Entry e))
            {
                e = new Entry { Mesh = new Mesh { name = "WorldTile " + key + " " + lod, hideFlags = HideFlags.DontSave } };
                entries.Add(code, e);
                Rebuild(e, lod, key);
            }
            else if (e.L != L && rebuildsLeft > 0)
            {
                rebuildsLeft--;
                Rebuild(e, lod, key);
            }
            triangles = e.Triangles;
            return e.Mesh;
        }

        void Rebuild(Entry e, TileLod lod, in WorldTileKey key)
        {
            e.Triangles = builder.Build(Q, L, lod, key).Triangles;
            AtlasMesh.Apply(builder.Data, e.Mesh);
            e.L = L;
            Builds++;
        }

        public void Dispose()
        {
            foreach (var pair in entries)
            {
                Mesh m = pair.Value.Mesh;
                if (m == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(m); else UnityEngine.Object.DestroyImmediate(m);
            }
            entries.Clear();
        }
    }
}
