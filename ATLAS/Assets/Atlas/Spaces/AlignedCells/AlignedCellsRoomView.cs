using System;
using Atlas.Rendering;
using Atlas.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Spaces.AlignedCells
{
    // Master Plan §4.7, §4.9, §8.3, §8.5 S3 row; task P1-B12.
    // Home walls, floor and every RoomModel object are drawn once per eye, unclipped, at their true anchor-frame position,
    // whenever any instance of the object's cell is drawn; never duplicated per instance (§4.7). One renderer per object,
    // parented to the root, never to a cell instance (furniture is registered, not tiled).
    public sealed class AlignedCellsRoomView : IDisposable
    {
        public const float HomeFloorDrop = 0.005f;   // m, [P1-B12 D2]

        readonly Transform root;
        readonly IHomeShell home;
        readonly CellGrid grid;
        readonly Material material;                  // [P1-B12 D3] one per-room instance, no property blocks
        readonly GameObject walls, floor;
        readonly GameObject[] objectGos;
        readonly MeshFilter[] objectFilters;
        readonly MeshRenderer[] objectRenderers;
        readonly CellRange[] ranges;
        readonly bool[] mask;
        readonly Action<int> onMeshChanged;
        bool disposed;

        public Material Material => material;
        public MeshRenderer WallsRenderer { get; }
        public MeshRenderer FloorRenderer { get; }
        public int ObjectCount { get; }
        public int VisibleObjectCount { get; private set; }

        public AlignedCellsRoomView(Transform root, Material template, IHomeShell home, CellGrid grid)
        {
            this.root = root; this.home = home; this.grid = grid;
            material = new Material(template) { hideFlags = HideFlags.DontSave };
            material.SetMatrix(AtlasShaderIDs.Gates, Matrix4x4.zero);   // §4.7 unclipped
            material.SetFloat(AtlasShaderIDs.WarpMode, 0);

            walls = MakeObject("HomeWalls", home.Walls, Vector3.zero, Quaternion.identity, out var wf, out var wr);
            WallsRenderer = wr;
            floor = MakeObject("HomeFloor", home.Floor, new Vector3(0, -HomeFloorDrop, 0), Quaternion.identity, out var ff, out var fr);
            FloorRenderer = fr;

            ObjectCount = home.ObjectCount;
            objectGos = new GameObject[ObjectCount];
            objectFilters = new MeshFilter[ObjectCount];
            objectRenderers = new MeshRenderer[ObjectCount];
            ranges = new CellRange[ObjectCount];
            for (int i = 0; i < ObjectCount; i++)
            {
                RoomObject rec = home.ObjectRecord(i);
                objectGos[i] = MakeObject("Object" + i, home.ObjectMesh(i), new Vector3(rec.position.x, 0, rec.position.z),
                                          RoomObjectCells.Rotation(rec), out objectFilters[i], out objectRenderers[i]);
                ranges[i] = RoomObjectCells.Of(grid, rec);
            }
            mask = new bool[grid.CountX * grid.CountZ];
            onMeshChanged = i =>   // §4.5: placeholder to generated swap; the pose is untouched
            {
                if (disposed || i < 0 || i >= ObjectCount || objectFilters[i] == null) return;
                objectFilters[i].sharedMesh = home.ObjectMesh(i);
            };
            home.ObjectMeshChanged += onMeshChanged;
        }

        GameObject MakeObject(string name, Mesh mesh, Vector3 pos, Quaternion rot, out MeshFilter mf, out MeshRenderer mr)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            var tr = go.transform;
            tr.SetParent(root, false);
            tr.localPosition = pos; tr.localRotation = rot;
            mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = mesh;
            mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        public MeshRenderer ObjectRenderer(int i) => objectRenderers[i];
        public CellRange ObjectCells(int i) => ranges[i];

        // §4.7: visible iff any instance of one of its cells is drawn. No allocation; writes only on change.
        public void Apply(CellInstanceList instances)
        {
            Array.Clear(mask, 0, mask.Length);
            int n = instances.Count, cx = grid.CountX;
            for (int k = 0; k < n; k++)
            {
                ref readonly CellInstance inst = ref instances[k];
                mask[inst.Cell.y * cx + inst.Cell.x] = true;
            }
            int visible = 0;
            for (int i = 0; i < ObjectCount; i++)
            {
                CellRange r = ranges[i];
                bool v = false;
                for (int z = r.Min.y; z <= r.Max.y && !v; z++)
                    for (int x = r.Min.x; x <= r.Max.x; x++)
                        if (mask[z * cx + x]) { v = true; break; }
                if (objectRenderers[i].enabled != v) objectRenderers[i].enabled = v;
                if (v) visible++;
            }
            VisibleObjectCount = visible;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            home.ObjectMeshChanged -= onMeshChanged;
            Kill(walls); Kill(floor);
            for (int i = 0; i < ObjectCount; i++) Kill(objectGos[i]);
            Kill(material);
        }

        static void Kill(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
