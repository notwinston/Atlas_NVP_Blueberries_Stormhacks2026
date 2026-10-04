// P1-B07: draws the generated world. Each drawn tile is one object with one pooled material (MP 8.3) and one merged mesh
// (shell, the kerbs the tile owns, its wedges and its archetype's props: one draw call per tile and eye, MP 8.5), anchored
// like a TileRenderer slot and tinted by its region. It takes the drawn list of the tile renderer's TileSelection, so the
// draw rule, the anchors and the levels of detail are the tile renderer's; that renderer runs with DrawShells off.
// A slot and its material are made the first time that many tiles are drawn, not up front. No Update: the space calls Draw
// at step 6 (MP 6.3), after TileRenderer.Upload.
using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.World;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Rendering
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class WorldContentRenderer : MonoBehaviour, ITileDrawStats
    {
        public const int MaxTiles = TileSelection.MaxDrawn;
        static readonly ProfilerMarker DrawMarker = new ProfilerMarker("Atlas.World.Draw");

        WorldGenerator world;
        WorldTileMeshCache meshes;
        MaterialPool pool;
        readonly List<MeshRenderer> slots = new List<MeshRenderer>(MaxTiles);
        readonly MeshFilter[] filters = new MeshFilter[MaxTiles];
        readonly Material[] mats = new Material[MaxTiles];
        readonly Mesh[] slotMesh = new Mesh[MaxTiles];
        readonly int[] slotRegion = new int[MaxTiles];
        readonly bool[] slotOn = new bool[MaxTiles];
        int tilesDrawn, trianglesDrawn;

        public WorldGenerator World => world;
        public WorldTileMeshCache Meshes => meshes;
        public IReadOnlyList<MeshRenderer> Slots => slots;
        public bool Configured => world != null;
        public int TilesDrawn => tilesDrawn;
        public int TrianglesDrawn => trianglesDrawn;
        /// <summary>Materials this renderer holds from the pool: the most tiles drawn in one frame since Configure or Trim.</summary>
        public int MaterialsRented => slots.Count;

        /// <summary>The world to draw, at length scale L metres. Rents nothing yet. Publishes itself as ITileDrawStats.</summary>
        public void Configure(WorldGenerator world, double lengthScale, MaterialPool pool)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            Release();
            this.world = world; this.pool = pool;
            meshes = new WorldTileMeshCache(world.Graph.Q, lengthScale);
            AtlasServices.Register<ITileDrawStats>(this);
        }

        /// <summary>Follows the curvature dial: meshes are rebuilt in place as they are next drawn.</summary>
        public void SetLengthScale(double lengthScale)
        {
            if (meshes != null) meshes.SetLengthScale(lengthScale);
        }

        /// <summary>MP 6.3 step 6: the drawn tiles of a TileSelection (Drawn, DrawnCount), in its order.</summary>
        public void Draw(TileDraw[] drawn, int count)
        {
            if (world == null) throw new InvalidOperationException("WorldContentRenderer.Draw before Configure");
            if (drawn == null) throw new ArgumentNullException(nameof(drawn));
            using (DrawMarker.Auto())
            {
                meshes.BeginFrame();
                int n = Math.Min(Math.Min(count, drawn.Length), MaxTiles), triangles = 0;
                for (int i = 0; i < n; i++)
                {
                    if (i == slots.Count) AddSlot();
                    mats[i].SetMatrix(AtlasShaderIDs.Anchor, WarpAnchor.ToMatrix(drawn[i].Anchor));
                    TileContent content = world.Content(drawn[i].Id);
                    Mesh mesh = meshes.Get(drawn[i].Lod, WorldTileKey.From(content), out int t);
                    triangles += t;
                    if (!ReferenceEquals(slotMesh[i], mesh)) { filters[i].sharedMesh = mesh; slotMesh[i] = mesh; }
                    if (slotRegion[i] != content.Region) { MaterialPool.SetTint(mats[i], WorldRegionTint.Linear(content.Region)); slotRegion[i] = content.Region; }
                    if (!slotOn[i]) { slots[i].enabled = true; slotOn[i] = true; }
                }
                for (int i = n; i < slots.Count; i++)
                    if (slotOn[i]) { slots[i].enabled = false; slotOn[i] = false; }
                tilesDrawn = n; trianglesDrawn = triangles;
            }
        }

        /// <summary>Draws nothing until the next Draw (curvature off, or the space is fading).</summary>
        public void Hide()
        {
            for (int i = 0; i < slots.Count; i++)
                if (slotOn[i]) { slots[i].enabled = false; slotOn[i] = false; }
            tilesDrawn = 0; trianglesDrawn = 0;
        }

        /// <summary>Gives back the materials of the slots beyond the tiles last drawn (after cullRho went down).</summary>
        public void Trim()
        {
            for (int i = slots.Count - 1; i >= tilesDrawn; i--) RemoveSlot(i);
        }

        /// <summary>Destroys the tile objects and meshes and returns every material to the pool.</summary>
        public void Release()
        {
            for (int i = slots.Count - 1; i >= 0; i--) RemoveSlot(i);
            meshes?.Dispose(); meshes = null;
            if (world != null) AtlasServices.Unregister<ITileDrawStats>(this);
            world = null; pool = null; tilesDrawn = 0; trianglesDrawn = 0;
        }

        void OnDestroy() => Release();

        void AddSlot()
        {
            int i = slots.Count;
            var child = new GameObject("WorldTile" + i.ToString("000")) { hideFlags = HideFlags.DontSave };
            child.transform.SetParent(transform, false);   // identity transform: the warp ignores it
            filters[i] = child.AddComponent<MeshFilter>();
            var r = child.AddComponent<MeshRenderer>();
            Material mat = pool.Rent();
            mat.SetFloat(AtlasShaderIDs.WarpMode, AtlasWarpCpu.ModeHyperbolic);
            MaterialPool.SetLines(mat, TileRenderer.EdgeLineStrength, 0f, float2.zero);
            MaterialPool.SetSurface(mat, nearFadeMetres: (float)CurvedRoomConstants.NearFadeRadiusM);   // MP 3.5: solid things fade within 0.3 m of the head
            r.sharedMaterial = mat;
            AtlasWarpBounds.Apply(r);
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.enabled = false;
            mats[i] = mat; slotMesh[i] = null; slotRegion[i] = -1; slotOn[i] = false;
            slots.Add(r);
        }

        void RemoveSlot(int i)
        {
            if (slots[i] != null)
            {
                if (Application.isPlaying) Destroy(slots[i].gameObject); else DestroyImmediate(slots[i].gameObject);
            }
            if (mats[i] != null && pool != null && pool.Owns(mats[i])) pool.Return(mats[i]);
            filters[i] = null; mats[i] = null; slotMesh[i] = null; slotOn[i] = false;
            slots.RemoveAt(i);
        }
    }
}
