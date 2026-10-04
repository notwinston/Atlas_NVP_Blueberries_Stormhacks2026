using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Rendering
{
    // P1-B11: the Home rebuild placed in curved space by the exp map about the room centre (Master Plan §4.6): the room centre is
    // tile zero's centre and room metres are tile zero's chart metres. Floor, walls and one renderer per object, each with its own
    // material instance (§8.3: no MaterialPropertyBlock) and 1000 m local bounds. Anchors are computed in double and cast last
    // (WarpAnchor.ToMatrix). The parent sits at the identity transform (assumption A2: world = anchor frame).
    //   kappa = 0: exactly Home (warp mode 0): floor and walls at the identity transform, object i at (record.position, yaw).
    //   kappa != 0: walls sink to a kerb [D10] and the Home floor is hidden (tile zero's own floor is drawn by the tile renderer);
    //   the kerb is anchored U^-1 F0; furniture is rigid: anchor U^-1 F0 Object(pos, yaw) so each object is a rigid copy about its base.
    public sealed class CurvedRoomView : IDisposable
    {
        readonly IHomeShell home;
        readonly GameObject root;
        readonly Part floor, walls, kerb;
        readonly List<Part> objects = new List<Part>();
        readonly Mesh kerbMesh;
        bool disposed;

        sealed class Part
        {
            public GameObject Go; public MeshFilter Filter; public MeshRenderer Renderer; public Material Material;
        }

        public CurvedRoomView(Transform parent, Material shared, IHomeShell home, Wall[] walls)
        {
            if (shared == null) throw new ArgumentNullException(nameof(shared));
            this.home = home ?? throw new ArgumentNullException(nameof(home));
            root = new GameObject("CurvedRoom") { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(parent, false);
            floor = Make("HomeFloor", shared, home.Floor);
            this.walls = Make("HomeWalls", shared, home.Walls);
            kerbMesh = new Mesh { name = "KerbMesh", hideFlags = HideFlags.DontSave };
            KerbMesh.Build(walls, CurvedRoomConstants.KerbHeightM, kerbMesh);
            kerb = Make("Kerb", shared, kerbMesh);
            for (int i = 0; i < home.ObjectCount; i++) objects.Add(Make("Object" + i, shared, home.ObjectMesh(i)));
            home.ObjectMeshChanged += OnObjectMeshChanged;
        }

        Part Make(string name, Material shared, Mesh mesh)
        {
            var p = new Part { Go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = HideFlags.DontSave } };
            p.Go.transform.SetParent(root.transform, false);
            p.Filter = p.Go.GetComponent<MeshFilter>(); p.Filter.sharedMesh = mesh;
            p.Renderer = p.Go.GetComponent<MeshRenderer>();
            p.Material = new Material(shared) { hideFlags = HideFlags.DontSave };
            p.Renderer.sharedMaterial = p.Material;
            AtlasWarpBounds.Apply(p.Renderer);
            p.Renderer.shadowCastingMode = ShadowCastingMode.Off; p.Renderer.receiveShadows = false;
            p.Renderer.enabled = false;
            return p;
        }

        void OnObjectMeshChanged(int i)
        {
            if (disposed || i < 0 || i >= objects.Count) return;
            objects[i].Filter.sharedMesh = home.ObjectMesh(i);
        }

        public int ObjectCount => objects.Count;
        public MeshRenderer FloorRenderer => floor.Renderer;
        public MeshRenderer WallsRenderer => walls.Renderer;
        public MeshRenderer KerbRenderer => kerb.Renderer;
        public MeshRenderer ObjectRenderer(int i) => objects[i].Renderer;
        public Mesh KerbMeshAsset => kerbMesh;

        /// <summary>
        /// Once per frame (MP §6.3 step 6). walker is U in the current tile, tileZeroInCurrent is F of tile zero seen from it.
        /// drawRoom = the room can be in view (the caller's distance test, [B09 D12]); false hides everything.
        /// </summary>
        public void Upload(double kappa, double lengthScaleM, in Iso2 walker, in Iso2 tileZeroInCurrent, bool drawRoom)
        {
            if (disposed) return;
            if (kappa == 0)
            {
                SetMode(floor, AtlasWarpCpu.ModeNone); SetMode(walls, AtlasWarpCpu.ModeNone); SetMode(kerb, AtlasWarpCpu.ModeNone);
                floor.Renderer.enabled = drawRoom && floor.Filter.sharedMesh != null;
                walls.Renderer.enabled = drawRoom && walls.Filter.sharedMesh != null;
                kerb.Renderer.enabled = false;
                floor.Go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                walls.Go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                for (int i = 0; i < objects.Count; i++)
                {
                    var o = home.ObjectRecord(i);
                    var p = objects[i];
                    p.Material.SetFloat(AtlasShaderIDs.WarpMode, AtlasWarpCpu.ModeNone);
                    p.Go.transform.SetPositionAndRotation(o.position, Quaternion.Euler(0f, o.yaw * Mathf.Rad2Deg, 0f));
                    p.Renderer.enabled = drawRoom && p.Filter.sharedMesh != null;
                }
                return;
            }
            float mode = kappa < 0 ? AtlasWarpCpu.ModeHyperbolic : AtlasWarpCpu.ModeSpherical;
            floor.Renderer.enabled = false; walls.Renderer.enabled = false;    // [D10]
            Iso2 a0 = walker.Inverse() * tileZeroInCurrent;                     // double
            kerb.Go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            kerb.Material.SetFloat(AtlasShaderIDs.WarpMode, mode);
            kerb.Material.SetMatrix(AtlasShaderIDs.Anchor, WarpAnchor.ToMatrix(a0));
            kerb.Renderer.enabled = drawRoom && kerbMesh.vertexCount > 0;
            for (int i = 0; i < objects.Count; i++)
            {
                var o = home.ObjectRecord(i);
                var p = objects[i];
                p.Go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                p.Material.SetFloat(AtlasShaderIDs.WarpMode, mode);
                Iso2 a = a0 * TileZeroPlacement.Object(new double2(o.position.x, o.position.z), o.yaw, kappa, lengthScaleM);
                p.Material.SetMatrix(AtlasShaderIDs.Anchor, WarpAnchor.ToMatrix(a));
                p.Renderer.enabled = drawRoom && p.Filter.sharedMesh != null;
            }
        }

        static void SetMode(Part p, float mode) => p.Material.SetFloat(AtlasShaderIDs.WarpMode, mode);

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            home.ObjectMeshChanged -= OnObjectMeshChanged;
            foreach (var p in objects) Kill(p.Material);
            Kill(floor.Material); Kill(walls.Material); Kill(kerb.Material); Kill(kerbMesh);
            Kill(root);
            objects.Clear();
        }

        static void Kill(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
