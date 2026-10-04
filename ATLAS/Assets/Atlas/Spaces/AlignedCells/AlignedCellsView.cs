using System;
using Atlas.Rendering;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Spaces.AlignedCells
{
    // Master Plan §4.7, §8.3: one renderer and one pooled material instance per cell instance; no MaterialPropertyBlock.
    // Materials are Atlas/Surface instances rented from the MaterialPool as instances are first drawn; the gates go into the
    // frozen block's _Gate0 .. _Gate3 through MaterialPool.SetGate. A cell takes its tile's region tint, as the same tile does in S2.
    public sealed class AlignedCellsView : IDisposable
    {
        readonly Transform root;
        readonly MaterialPool pool;
        readonly Mesh cellMesh, postsMesh;
        readonly GameObject[] objects = new GameObject[SightLines.MaxInstances];
        readonly Transform[] transforms = new Transform[SightLines.MaxInstances];
        readonly MeshRenderer[] renderers = new MeshRenderer[SightLines.MaxInstances];
        readonly Material[] materials = new Material[SightLines.MaxInstances];
        readonly GameObject postsObject;
        readonly Material postsMaterial;
        readonly CellGrid grid;
        bool disposed;

        public int ActiveInstanceCount { get; private set; }
        public int MaterialsRented { get; private set; }
        public int CellTriangles { get; }
        public int PostTriangles { get; }

        public AlignedCellsView(Transform root, MaterialPool pool, CellGrid grid)
        {
            this.root = root; this.grid = grid;
            this.pool = pool ?? throw new ArgumentNullException(nameof(pool));
            cellMesh = new Mesh { name = "AlignedCell", hideFlags = HideFlags.HideAndDontSave };
            CellTriangles = new TileMeshGenerator(TilePalette.Default).Build(TileMeshSpec.FlatCell(grid.Side, kerbMask: 0, postMask: 0), cellMesh).TriangleCount;
            var data = new AtlasMeshBuilder(256, 1024);
            PostTriangles = PostMesh.Build(grid, TilePalette.Default, data);
            postsMesh = AtlasMesh.Create(data, "AlignedPosts");
            for (int i = 0; i < objects.Length; i++)
            {
                objects[i] = MakeObject("Cell" + i, cellMesh, out transforms[i], out renderers[i]);
                objects[i].SetActive(false);
            }
            postsMaterial = Rent();
            postsObject = MakeObject("Posts", postsMesh, out _, out MeshRenderer posts);
            posts.sharedMaterial = postsMaterial;
        }

        Material Rent()
        {
            MaterialsRented++;
            return pool.Rent();
        }

        GameObject MakeObject(string name, Mesh mesh, out Transform tr, out MeshRenderer mr)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            tr = go.transform;
            tr.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        // Cast to float only here (kernel.md §3). Gate posts are uploaded in world coordinates: the shader tests them against
        // each eye's own camera position.
        public void Apply(CellInstanceList list)
        {
            int n = list.Count;
            for (int i = 0; i < n; i++)
            {
                ref readonly CellInstance inst = ref list[i];
                Material mat = materials[i];
                if (mat == null)
                {
                    mat = materials[i] = Rent();
                    MaterialPool.SetLines(mat, TileRenderer.EdgeLineStrength, 0f, float2.zero);   // the cell's edges
                    renderers[i].sharedMaterial = mat;
                }
                if (!objects[i].activeSelf) objects[i].SetActive(true);
                double2 c = grid.CellCentre(inst.Cell);
                var tr = transforms[i];
                tr.localPosition = new Vector3((float)c.x, 0, (float)c.y);
                tr.localRotation = Quaternion.Euler(0, -90f * inst.Frame.Turn, 0);
                MaterialPool.ClearGates(mat);
                for (int g = 0; g < inst.GateCount; g++)
                {
                    double4 gate = inst.Gate(g);
                    Vector3 w = root.TransformPoint((float)gate.x, 0, (float)gate.y);
                    MaterialPool.SetGate(mat, g, new float2(w.x, w.z), (float)gate.z);
                }
                MaterialPool.SetTint(mat, WorldRegionTint.Linear(Regions.RegionOf(inst.Frame.Tile)));
            }
            for (int i = n; i < objects.Length; i++)
                if (objects[i].activeSelf) objects[i].SetActive(false);
            ActiveInstanceCount = n;
        }

        public Material InstanceMaterial(int i) => materials[i];
        public Material PostsMaterial => postsMaterial;

        // Destroys the objects and meshes and gives every material back to the pool.
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            for (int i = 0; i < objects.Length; i++)
            {
                Kill(objects[i]);
                if (materials[i] != null) pool.Return(materials[i]);
                materials[i] = null;
            }
            Kill(postsObject);
            if (postsMaterial != null) pool.Return(postsMaterial);
            Kill(cellMesh); Kill(postsMesh);
            MaterialsRented = 0; ActiveInstanceCount = 0;
        }

        static void Kill(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
