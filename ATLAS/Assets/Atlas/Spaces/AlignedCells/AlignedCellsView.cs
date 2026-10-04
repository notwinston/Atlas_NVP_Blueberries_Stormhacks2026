using System;
using Atlas.Rendering;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Spaces.AlignedCells
{
    // Master Plan §4.7, §8.3: one pooled renderer and one material instance per cell instance; no MaterialPropertyBlock.
    public sealed class AlignedCellsView : IDisposable
    {
        readonly Transform root;
        readonly Mesh cellMesh, postsMesh;
        readonly GameObject[] objects = new GameObject[SightLines.MaxInstances];
        readonly Transform[] transforms = new Transform[SightLines.MaxInstances];
        readonly Material[] materials = new Material[SightLines.MaxInstances];
        readonly GameObject postsObject;
        readonly Material postsMaterial;
        readonly CellGrid grid;
        readonly Matrix4x4 zero = new Matrix4x4();
        bool disposed;

        public int ActiveInstanceCount { get; private set; }

        public AlignedCellsView(Transform root, Material template, CellGrid grid)
        {
            this.root = root; this.grid = grid;
            cellMesh = new Mesh { name = "AlignedCell" };
            new TileMeshGenerator(TilePalette.Provisional).Build(TileMeshSpec.FlatCell(grid.Side, kerbMask: 0, postMask: 0), cellMesh);
            postsMesh = new Mesh { name = "AlignedPosts" };
            PostMesh.Build(grid, TilePalette.Provisional, postsMesh);
            for (int i = 0; i < objects.Length; i++)
            {
                materials[i] = new Material(template) { hideFlags = HideFlags.DontSave };
                objects[i] = MakeObject("Cell" + i, cellMesh, materials[i], out transforms[i]);
                objects[i].SetActive(false);
            }
            postsMaterial = new Material(template) { hideFlags = HideFlags.DontSave };
            postsObject = MakeObject("Posts", postsMesh, postsMaterial, out _);
            postsMaterial.SetMatrix(AtlasShaderIDs.Gates, zero);
            postsMaterial.SetFloat(AtlasShaderIDs.WarpMode, 0);
        }

        GameObject MakeObject(string name, Mesh mesh, Material mat, out Transform tr)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            tr = go.transform;
            tr.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        // Cast to float only here (kernel.md §3).
        public void Apply(CellInstanceList list)
        {
            int n = list.Count;
            for (int i = 0; i < n; i++)
            {
                ref readonly CellInstance inst = ref list[i];
                objects[i].SetActive(true);
                double2 c = grid.CellCentre(inst.Cell);
                var tr = transforms[i];
                tr.localPosition = new Vector3((float)c.x, 0, (float)c.y);
                tr.localRotation = Quaternion.Euler(0, -90f * inst.Frame.Turn, 0);
                var m = new Matrix4x4();
                for (int g = 0; g < inst.GateCount; g++)
                {
                    double4 gate = inst.Gate(g);
                    Vector3 w = root.TransformPoint((float)gate.x, 0, (float)gate.y);
                    m[g, 0] = w.x; m[g, 1] = w.z; m[g, 2] = (float)gate.z;
                }
                materials[i].SetMatrix(AtlasShaderIDs.Gates, m);
                materials[i].SetFloat(AtlasShaderIDs.WarpMode, 0);
            }
            for (int i = n; i < objects.Length; i++) objects[i].SetActive(false);
            ActiveInstanceCount = n;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            for (int i = 0; i < objects.Length; i++) { Kill(objects[i]); Kill(materials[i]); }
            Kill(postsObject); Kill(postsMaterial); Kill(cellMesh); Kill(postsMesh);
        }

        static void Kill(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
