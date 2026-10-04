using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Rendering;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Spaces.SmallPlanet
{
    // §8.3: six tiles plus the beacon, one pooled material instance each, no MaterialPropertyBlock; driven by the frame loop (§6.3), no Update().
    public sealed class SmallPlanetView : MonoBehaviour
    {
        readonly List<MeshRenderer> tiles = new List<MeshRenderer>(6);
        readonly List<Material> materials = new List<Material>(7);
        readonly List<GameObject> children = new List<GameObject>(7);
        readonly TileMeshGenerator generator = new TileMeshGenerator(TilePalette.Default);
        MaterialPool pool;
        Mesh tileMesh, beaconMesh;
        MeshRenderer beacon;
        bool shown = true;

        public IReadOnlyList<MeshRenderer> TileRenderers => tiles;
        public MeshRenderer BeaconRenderer => beacon;
        public bool Built => tileMesh != null;
        /// <summary>Triangles of one tile, as built.</summary>
        public int TileTriangles { get; private set; }

        static void Kill(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        MeshRenderer MakeChild(string name, Mesh mesh)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.GetComponent<MeshRenderer>();
            Material m = pool.Rent();
            MaterialPool.SetWarpMode(m, (int)AtlasWarpCpu.ModeSpherical);
            mr.sharedMaterial = m;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            AtlasWarpBounds.Apply(mr);
            materials.Add(m);
            children.Add(go);
            return mr;
        }

        public void Build(double radius, MaterialPool materialPool)
        {
            Release();
            if (!(radius > 0)) throw new ArgumentOutOfRangeException(nameof(radius));
            pool = materialPool ?? throw new ArgumentNullException(nameof(materialPool));
            tileMesh = new Mesh { name = "SmallPlanetTile", hideFlags = HideFlags.DontSave };
            BuildTile(radius);
            var data = new AtlasMeshBuilder(32, 96);
            PoleBeaconMesh.Build(data);
            beaconMesh = AtlasMesh.Create(data, "PoleBeacon");
            for (int f = 0; f < CubeTiling.FaceCount; f++)
            {
                MeshRenderer tile = MakeChild("Tile" + f, tileMesh);
                MaterialPool.SetLines(tile.sharedMaterial, TileRenderer.EdgeLineStrength, 0f, float2.zero);
                tiles.Add(tile);
            }
            beacon = MakeChild("PoleBeacon", beaconMesh);
            shown = true;
        }

        /// <summary>A planet of another radius: the one tile mesh is rebuilt in place.</summary>
        public void SetRadius(double radius)
        {
            if (tileMesh == null) throw new InvalidOperationException("SetRadius before Build");
            BuildTile(radius);
        }

        // §3.4, §8.4: Near LOD on all six, no kerbs (§3.5 open sight lines), posts at every corner
        void BuildTile(double radius)
        {
            TileTriangles = generator.Build(TileMeshSpec.Spherical(radius, TileLod.Near, 0, 0xF), tileMesh).TriangleCount;
        }

        public bool Shown
        {
            get => shown;
            set
            {
                if (shown == value) return;
                shown = value;
                for (int i = 0; i < children.Count; i++) children[i].SetActive(value);
            }
        }

        public void Upload(Iso2[] tileAnchors, in Iso2 beaconAnchor)
        {
            for (int f = 0; f < tiles.Count; f++) tiles[f].sharedMaterial.SetMatrix(AtlasShaderIDs.Anchor, WarpAnchor.ToMatrix(tileAnchors[f]));
            beacon.sharedMaterial.SetMatrix(AtlasShaderIDs.Anchor, WarpAnchor.ToMatrix(beaconAnchor));
        }

        public void Release()
        {
            for (int i = 0; i < children.Count; i++) Kill(children[i]);
            if (pool != null)
                for (int i = 0; i < materials.Count; i++)
                    if (pool.Owns(materials[i])) pool.Return(materials[i]);
            Kill(tileMesh); Kill(beaconMesh);
            children.Clear(); materials.Clear(); tiles.Clear();
            tileMesh = null; beaconMesh = null; beacon = null; pool = null;
        }

        void OnDestroy() => Release();
    }
}
