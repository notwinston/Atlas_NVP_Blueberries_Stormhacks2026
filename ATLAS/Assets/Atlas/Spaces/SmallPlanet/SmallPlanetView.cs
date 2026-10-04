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
    // With the PlanetKit tiles modelled in Blender (PlanetKit.md) each face draws its own mesh and the kit's cairn
    // replaces the pole beacon; without them, one procedural tile mesh is drawn six times with the beacon, as before.
    public sealed class SmallPlanetView : MonoBehaviour
    {
        readonly List<MeshRenderer> tiles = new List<MeshRenderer>(6);
        readonly List<Material> materials = new List<Material>(7);
        readonly List<GameObject> children = new List<GameObject>(7);
        readonly Mesh[] faceMeshes = new Mesh[CubeTiling.FaceCount];
        readonly TileMeshGenerator generator = new TileMeshGenerator(TilePalette.Default);
        MaterialPool pool;
        Mesh tileMesh, beaconMesh;
        MeshRenderer beacon;
        PlanetKitTiles kit;
        AtlasMeshBuilder kitScratch;
        bool shown = true;

        static PlanetKitTiles s_SharedKit;
        static bool s_SharedKitTried;

        public IReadOnlyList<MeshRenderer> TileRenderers => tiles;
        /// <summary>The procedural pole beacon; null while the kit's tiles are drawn (their cairn is the beacon).</summary>
        public MeshRenderer BeaconRenderer => beacon;
        public bool Built => tiles.Count > 0;
        /// <summary>The faces draw the PlanetKit tiles; false on the procedural fallback.</summary>
        public bool UsesKit => kit != null;
        /// <summary>Triangles of one procedural tile, as built; with the kit, of the largest tile.</summary>
        public int TileTriangles { get; private set; }
        /// <summary>Triangles of all six tiles, as built.</summary>
        public int PlanetTriangles { get; private set; }

        /// <summary>
        /// The PlanetKit tiles from Resources/PlanetKit, read once per run; null (after one warning) when they are
        /// missing or refused, and the planet is then built from procedural tiles.
        /// </summary>
        public static PlanetKitTiles SharedKit()
        {
            if (!s_SharedKitTried)
            {
                s_SharedKitTried = true;
                s_SharedKit = LoadKit(ReadKitResource);
            }
            return s_SharedKit;
        }

        /// <summary>Reads the kit with this reader; null with one warning when it is refused.</summary>
        public static PlanetKitTiles LoadKit(Func<string, byte[]> read)
        {
            PlanetKitTiles loaded = PlanetKitTiles.Load(read, out string error);
            if (loaded == null)
                Debug.LogWarning("Atlas: the Small Planet kit tiles are missing or invalid; using the procedural tiles. (" + error + ")");
            return loaded;
        }

        internal static byte[] ReadKitResource(string name)
        {
            var asset = Resources.Load<TextAsset>(PlanetKitTiles.ResourceFolder + "/" + name);
            if (asset == null) return null;
            byte[] bytes = asset.bytes;
            Resources.UnloadAsset(asset);
            return bytes;
        }

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

        /// <summary>The planet at this radius, from the PlanetKit tiles when they load, else procedural.</summary>
        public void Build(double radius, MaterialPool materialPool) => Build(radius, materialPool, SharedKit());

        /// <summary>The planet at this radius from these kit tiles; null builds the procedural tiles and the pole beacon.</summary>
        public void Build(double radius, MaterialPool materialPool, PlanetKitTiles kitTiles)
        {
            Release();
            if (!(radius > 0)) throw new ArgumentOutOfRangeException(nameof(radius));
            pool = materialPool ?? throw new ArgumentNullException(nameof(materialPool));
            kit = kitTiles;
            if (kit != null)
            {
                kitScratch = new AtlasMeshBuilder(kit.MaxVertices, kit.MaxIndices);
                for (int f = 0; f < CubeTiling.FaceCount; f++)
                    faceMeshes[f] = new Mesh { name = "SmallPlanetTile" + f, hideFlags = HideFlags.DontSave };
            }
            else
            {
                tileMesh = new Mesh { name = "SmallPlanetTile", hideFlags = HideFlags.DontSave };
                for (int f = 0; f < CubeTiling.FaceCount; f++) faceMeshes[f] = tileMesh;
            }
            BuildTiles(radius);
            for (int f = 0; f < CubeTiling.FaceCount; f++)
            {
                MeshRenderer tile = MakeChild("Tile" + f, faceMeshes[f]);
                MaterialPool.SetLines(tile.sharedMaterial, TileRenderer.EdgeLineStrength, 0f, float2.zero);
                tiles.Add(tile);
            }
            if (kit == null)
            {
                var data = new AtlasMeshBuilder(32, 96);
                PoleBeaconMesh.Build(data);
                beaconMesh = AtlasMesh.Create(data, "PoleBeacon");
                beacon = MakeChild("PoleBeacon", beaconMesh);
            }
            shown = true;
        }

        /// <summary>A planet of another radius: the tile meshes are rebuilt in place.</summary>
        public void SetRadius(double radius)
        {
            if (!Built) throw new InvalidOperationException("SetRadius before Build");
            if (!(radius > 0)) throw new ArgumentOutOfRangeException(nameof(radius));
            BuildTiles(radius);
        }

        void BuildTiles(double radius)
        {
            if (kit == null)
            {
                // §3.4, §8.4: Near LOD on all six, no kerbs (§3.5 open sight lines), posts at every corner
                TileTriangles = generator.Build(TileMeshSpec.Spherical(radius, TileLod.Near, 0, 0xF), tileMesh).TriangleCount;
                PlanetTriangles = CubeTiling.FaceCount * TileTriangles;
                return;
            }
            // PlanetKit.md: xz is stored in units of R0, so x and z are multiplied by R; heights stay metres.
            int largest = 0;
            for (int f = 0; f < CubeTiling.FaceCount; f++)
            {
                kit.Scale(f, radius, kitScratch);
                AtlasMesh.Apply(kitScratch, faceMeshes[f]);
                largest = math.max(largest, kitScratch.TriangleCount);
            }
            TileTriangles = largest;
            PlanetTriangles = kit.TotalTriangles;
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
            if (beacon != null) beacon.sharedMaterial.SetMatrix(AtlasShaderIDs.Anchor, WarpAnchor.ToMatrix(beaconAnchor));
        }

        public void Release()
        {
            for (int i = 0; i < children.Count; i++) Kill(children[i]);
            if (pool != null)
                for (int i = 0; i < materials.Count; i++)
                    if (pool.Owns(materials[i])) pool.Return(materials[i]);
            if (kit != null)
                for (int f = 0; f < faceMeshes.Length; f++) Kill(faceMeshes[f]);
            Kill(tileMesh); Kill(beaconMesh);
            Array.Clear(faceMeshes, 0, faceMeshes.Length);
            children.Clear(); materials.Clear(); tiles.Clear();
            tileMesh = null; beaconMesh = null; beacon = null; pool = null; kit = null; kitScratch = null;
            TileTriangles = 0; PlanetTriangles = 0;
        }

        void OnDestroy() => Release();
    }
}
