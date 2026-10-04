using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Rendering;
using UnityEngine;

namespace Atlas.Spaces.SmallPlanet
{
    // §8.3: six tiles plus the beacon, one material instance each, no MaterialPropertyBlock; driven by the frame loop (§6.3), no Update().
    public sealed class SmallPlanetView : MonoBehaviour
    {
        readonly List<MeshRenderer> tiles = new List<MeshRenderer>(6);
        readonly List<Material> materials = new List<Material>(7);
        readonly Iso2[] anchors = new Iso2[CubeTiling.FaceCount];
        Mesh tileMesh, beaconMesh;
        MeshRenderer beacon;
        readonly List<GameObject> children = new List<GameObject>(7);

        public IReadOnlyList<MeshRenderer> TileRenderers => tiles;
        public MeshRenderer BeaconRenderer => beacon;

        static void Kill(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        MeshRenderer MakeChild(string name, Mesh mesh, Material shared)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.GetComponent<MeshRenderer>();
            var m = new Material(shared) { hideFlags = HideFlags.DontSave };
            m.SetFloat(AtlasShaderIDs.WarpMode, AtlasWarpCpu.ModeSpherical);
            mr.sharedMaterial = m;
            AtlasWarpBounds.Apply(mr);
            materials.Add(m);
            children.Add(go);
            return mr;
        }

        public void Build(SmallPlanetSpace space, Material shared)
        {
            Release();
            if (!space.Configured) throw new InvalidOperationException("space not configured");
            tileMesh = new Mesh { name = "SmallPlanetTile", hideFlags = HideFlags.DontSave };
            // §3.4, §8.4: Near LOD on all six, no kerbs (§3.5 open sight lines), posts at every corner
            new TileMeshGenerator(TilePalette.Provisional).Build(TileMeshSpec.Spherical(space.R, TileLod.Near, 0, 0xF), tileMesh);
            beaconMesh = new Mesh { name = "PoleBeacon", hideFlags = HideFlags.DontSave };
            PoleBeaconMesh.Build(beaconMesh);
            for (int f = 0; f < CubeTiling.FaceCount; f++) tiles.Add(MakeChild("Tile" + f, tileMesh, shared));
            beacon = MakeChild("PoleBeacon", beaconMesh, shared);
        }

        public void Upload(SmallPlanetSpace space, Vector3 headPosWS)
        {
            Shader.SetGlobalFloat(AtlasShaderIDs.HypL, (float)space.R);
            Shader.SetGlobalVector(AtlasShaderIDs.HeadPosWS, headPosWS);
            space.ComputeAnchors(anchors, out Iso2 b);
            for (int f = 0; f < tiles.Count; f++) tiles[f].sharedMaterial.SetMatrix(AtlasShaderIDs.Anchor, WarpAnchor.ToMatrix(anchors[f]));
            beacon.sharedMaterial.SetMatrix(AtlasShaderIDs.Anchor, WarpAnchor.ToMatrix(b));
        }

        public void Release()
        {
            for (int i = 0; i < children.Count; i++) Kill(children[i]);
            for (int i = 0; i < materials.Count; i++) Kill(materials[i]);
            Kill(tileMesh); Kill(beaconMesh);
            children.Clear(); materials.Clear(); tiles.Clear();
            tileMesh = null; beaconMesh = null; beacon = null;
        }

        void OnDestroy() => Release();
    }
}
