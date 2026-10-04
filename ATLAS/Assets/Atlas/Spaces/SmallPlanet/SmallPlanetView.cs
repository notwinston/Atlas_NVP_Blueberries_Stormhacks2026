using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Rendering;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Spaces.SmallPlanet
{
    // §8.3: the six PlanetKit tiles modelled in Blender (PlanetKit.md), one mesh and one pooled material instance a face,
    // no MaterialPropertyBlock; driven by the frame loop (§6.3), no Update(). The kit's cairn on face 5 is the planet's
    // landmark. There is no procedural fallback: without the kit nothing is drawn (one error is logged).
    public sealed class SmallPlanetView : MonoBehaviour
    {
        readonly List<MeshRenderer> tiles = new List<MeshRenderer>(6);
        readonly List<Material> materials = new List<Material>(6);
        readonly List<GameObject> children = new List<GameObject>(6);
        readonly Mesh[] faceMeshes = new Mesh[CubeTiling.FaceCount];
        MaterialPool pool;
        PlanetKitTiles kit;
        AtlasMeshBuilder kitScratch;
        bool shown = true;

        static PlanetKitTiles s_SharedKit;
        static bool s_SharedKitTried;

        public IReadOnlyList<MeshRenderer> TileRenderers => tiles;
        /// <summary>The six kit tiles are built; false when the kit could not be loaded (nothing is drawn).</summary>
        public bool Built => tiles.Count > 0;
        /// <summary>Triangles of the largest tile, as built.</summary>
        public int TileTriangles { get; private set; }
        /// <summary>Triangles of all six tiles, as built.</summary>
        public int PlanetTriangles { get; private set; }

        /// <summary>
        /// The PlanetKit tiles from Resources/PlanetKit, read once per run; null (after one error) when they are
        /// missing or refused, and the planet is then not drawn.
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

        /// <summary>Reads the kit with this reader; null with one error when it is refused.</summary>
        public static PlanetKitTiles LoadKit(Func<string, byte[]> read)
        {
            PlanetKitTiles loaded = PlanetKitTiles.Load(read, out string error);
            if (loaded == null)
                Debug.LogError("Atlas: the Small Planet kit tiles are missing or invalid; the planet is not drawn. (" + error + ")");
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

        /// <summary>The planet at this radius from the PlanetKit tiles in Resources; nothing when they do not load.</summary>
        public void Build(double radius, MaterialPool materialPool) => Build(radius, materialPool, SharedKit());

        /// <summary>The planet at this radius from these kit tiles; null draws nothing (<see cref="Built"/> stays false).</summary>
        public void Build(double radius, MaterialPool materialPool, PlanetKitTiles kitTiles)
        {
            Release();
            if (!(radius > 0)) throw new ArgumentOutOfRangeException(nameof(radius));
            pool = materialPool ?? throw new ArgumentNullException(nameof(materialPool));
            kit = kitTiles;
            if (kit == null) return;
            kitScratch = new AtlasMeshBuilder(kit.MaxVertices, kit.MaxIndices);
            for (int f = 0; f < CubeTiling.FaceCount; f++)
                faceMeshes[f] = new Mesh { name = "SmallPlanetTile" + f, hideFlags = HideFlags.DontSave };
            BuildTiles(radius);
            for (int f = 0; f < CubeTiling.FaceCount; f++)
            {
                MeshRenderer tile = MakeChild("Tile" + f, faceMeshes[f]);
                MaterialPool.SetLines(tile.sharedMaterial, TileRenderer.EdgeLineStrength, 0f, float2.zero);
                tiles.Add(tile);
            }
            shown = true;
        }

        /// <summary>A planet of another radius: the tile meshes are rescaled in place. Nothing to do when nothing is built.</summary>
        public void SetRadius(double radius)
        {
            if (!(radius > 0)) throw new ArgumentOutOfRangeException(nameof(radius));
            if (!Built) return;
            BuildTiles(radius);
        }

        // PlanetKit.md: xz is stored in units of R0, so x and z are multiplied by R; heights stay metres.
        void BuildTiles(double radius)
        {
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

        public void Upload(Iso2[] tileAnchors)
        {
            for (int f = 0; f < tiles.Count; f++) tiles[f].sharedMaterial.SetMatrix(AtlasShaderIDs.Anchor, WarpAnchor.ToMatrix(tileAnchors[f]));
        }

        public void Release()
        {
            for (int i = 0; i < children.Count; i++) Kill(children[i]);
            if (pool != null)
                for (int i = 0; i < materials.Count; i++)
                    if (pool.Owns(materials[i])) pool.Return(materials[i]);
            for (int f = 0; f < faceMeshes.Length; f++) Kill(faceMeshes[f]);
            Array.Clear(faceMeshes, 0, faceMeshes.Length);
            children.Clear(); materials.Clear(); tiles.Clear();
            pool = null; kit = null; kitScratch = null;
            TileTriangles = 0; PlanetTriangles = 0;
        }

        void OnDestroy() => Release();
    }
}
