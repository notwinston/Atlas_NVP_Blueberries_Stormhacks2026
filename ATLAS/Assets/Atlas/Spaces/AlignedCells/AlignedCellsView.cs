using System;
using Atlas.Rendering;
using Atlas.Telemetry;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Spaces.AlignedCells
{
    // Master Plan §4.7, §8.3: one renderer and one pooled material instance per cell instance; no MaterialPropertyBlock.
    // Materials are Atlas/Surface instances rented from the MaterialPool as instances are first drawn; the gates go into the
    // frozen block's _Gate0 .. _Gate3 through MaterialPool.SetGate. A cell takes its tile's region tint, as the same tile does in S2.
    // The duck maze (S3 duck maze design, 2026-10-04, §3): a cell takes the mesh of its tile's wall mask (16 meshes, kerbs on the
    // walled edges; mesh 0 is built with the view, masks 1 to 15 the first time a maze is drawn, so classic S3 builds one), a tile
    // off the maze is tinted Void, and the duck stands in every cell that shows its tile, gated
    // as that cell. The ducks are pooled (MaxDuckViews) and made, with their mesh, the first time a maze is drawn.
    public sealed class AlignedCellsView : IDisposable
    {
        public const int CellMeshCount = 16, MaxDuckViews = 8;
        /// <summary>The duck's turn, degrees per second, before and for FastSpinSeconds after the find.</summary>
        public const double DuckSpinRate = 40, DuckFoundSpinRate = 240, FastSpinSeconds = 2;
        /// <summary>The duck's bob: height above the floor and amplitude, metres, and period, seconds.</summary>
        public const double DuckBobHeight = 0.03, DuckBobAmplitude = 0.02, DuckBobPeriod = 1.6;
        /// <summary>The pop after the find: the final scale, and how long it takes, seconds (ease-out).</summary>
        public const double DuckPopScale = 1.4, DuckPopSeconds = 0.3;

        readonly Transform root;
        readonly MaterialPool pool;
        readonly Mesh[] cellMeshes = new Mesh[CellMeshCount];
        readonly TileMeshGenerator cellGenerator = new TileMeshGenerator(TilePalette.Default);
        readonly Mesh postsMesh;
        readonly GameObject[] objects = new GameObject[SightLines.MaxInstances];
        readonly Transform[] transforms = new Transform[SightLines.MaxInstances];
        readonly MeshRenderer[] renderers = new MeshRenderer[SightLines.MaxInstances];
        readonly MeshFilter[] filters = new MeshFilter[SightLines.MaxInstances];
        readonly int[] shownMask = new int[SightLines.MaxInstances];
        readonly Material[] materials = new Material[SightLines.MaxInstances];
        readonly GameObject postsObject;
        readonly Material postsMaterial;
        readonly CellGrid grid;
        readonly KitSet kit;
        // The ducks, made on first use.
        Mesh duckMesh;
        GameObject[] duckObjects;
        Transform[] duckTransforms;
        Material[] duckMaterials;
        readonly int[] duckTurn = new int[MaxDuckViews];
        float duckSpin, duckY = (float)DuckBobHeight, duckScale = 1f;
        bool disposed;

        public int ActiveInstanceCount { get; private set; }
        public int MaterialsRented { get; private set; }
        public int CellTriangles { get; }
        /// <summary>True once the kerbed cell meshes (masks 1 to 15) are built: the first time a maze is drawn.</summary>
        public bool KerbsBuilt { get; private set; }
        public int PostTriangles { get; }
        /// <summary>Ducks drawn by the last Apply: one per instance showing the duck's tile, at most MaxDuckViews.</summary>
        public int DuckViewCount { get; private set; }

        /// <summary>kit: where the duck is read from (piece "duck"); null draws the procedural duck.</summary>
        public AlignedCellsView(Transform root, MaterialPool pool, CellGrid grid, KitSet kit = null)
        {
            this.root = root; this.grid = grid; this.kit = kit;
            this.pool = pool ?? throw new ArgumentNullException(nameof(pool));
            CellTriangles = BuildCellMesh(0);
            var data = new AtlasMeshBuilder(256, 1024);
            PostTriangles = PostMesh.Build(grid, TilePalette.Default, data);
            postsMesh = AtlasMesh.Create(data, "AlignedPosts");
            for (int i = 0; i < objects.Length; i++)
            {
                objects[i] = MakeObject("Cell" + i, cellMeshes[0], out transforms[i], out renderers[i]);
                filters[i] = objects[i].GetComponent<MeshFilter>();
                objects[i].SetActive(false);
            }
            postsMaterial = Rent();
            postsObject = MakeObject("Posts", postsMesh, out _, out MeshRenderer posts);
            posts.sharedMaterial = postsMaterial;
        }

        // The flat cell with kerbs on the mesh edges of `mask`; its triangle count.
        int BuildCellMesh(int mask)
        {
            cellMeshes[mask] = new Mesh { name = "AlignedCell" + mask, hideFlags = HideFlags.HideAndDontSave };
            return cellGenerator.Build(TileMeshSpec.FlatCell(grid.Side, kerbMask: mask, postMask: 0), cellMeshes[mask]).TriangleCount;
        }

        void BuildKerbs()
        {
            if (KerbsBuilt) return;
            KerbsBuilt = true;
            for (int mask = 1; mask < CellMeshCount; mask++) BuildCellMesh(mask);
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

        /// <summary>
        /// The kerb mask of an instance's cell mesh (bit e = kerb on mesh edge e, before the instance's rotation) for its tile's
        /// wall mask (bit k = wall on tile edge k). Mesh edge e of a flat cell (TileMeshGenerator, kappa 0: the kerb stands on
        /// RotE((h, u), e), placed at (x, z) = Klein (x, y)) faces local direction e (0 +x, 1 +z, 2 -x, 3 -z), and the instance's
        /// rotation Quaternion.Euler(0, -90 * turn, 0) takes local direction j to room direction (j + turn) &amp; 3, which is where
        /// CellFrame puts tile edge j. The rotation does the turning: tile edge k is mesh edge k.
        /// </summary>
        public static int InstanceKerbMask(int tileWallMask, int turn) => tileWallMask & 0xF;

        /// <summary>The duck's turn about its axis, degrees in [0, 360): 40 deg/s, and 240 deg/s for 2 s after the find.</summary>
        public static float DuckSpin(double time, bool found, double foundTime)
        {
            double degrees = DuckSpinRate * time;
            if (found) degrees += (DuckFoundSpinRate - DuckSpinRate) * Math.Min(Math.Max(time - foundTime, 0), FastSpinSeconds);
            degrees %= 360;
            return (float)(degrees < 0 ? degrees + 360 : degrees);
        }

        /// <summary>The duck's height above the floor, metres: 0.03 + 0.02 sin(2 pi t / 1.6).</summary>
        public static float DuckBob(double time) => (float)(DuckBobHeight + DuckBobAmplitude * Math.Sin(2 * Math.PI * time / DuckBobPeriod));

        /// <summary>The duck's scale: 1, and after the find from 1 to 1.4 over 0.3 s, easing out.</summary>
        public static float DuckScale(double time, bool found, double foundTime)
        {
            if (!found) return 1f;
            double u = Math.Min(Math.Max((time - foundTime) / DuckPopSeconds, 0), 1);
            u = 1 - (1 - u) * (1 - u);
            return (float)(1 + (DuckPopScale - 1) * u);
        }

        /// <summary>Today's S3: no kerbs, no Void, no duck.</summary>
        public void Apply(CellInstanceList list) => Apply(list, null);

        // Cast to float only here (kernel.md §3). Gate posts are uploaded in world coordinates: the shader tests them against
        // each eye's own camera position. maze null: today's S3.
        public void Apply(CellInstanceList list, DuckMaze maze)
        {
            int n = list.Count, ducks = 0;
            if (maze != null) BuildKerbs();
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
                int mask = maze == null ? 0 : InstanceKerbMask(maze.WallMask(inst.Frame.Tile), inst.Frame.Turn);
                if (mask != shownMask[i]) { filters[i].sharedMesh = cellMeshes[mask]; shownMask[i] = mask; }
                double2 c = grid.CellCentre(inst.Cell);
                var tr = transforms[i];
                tr.localPosition = new Vector3((float)c.x, 0, (float)c.y);
                tr.localRotation = Quaternion.Euler(0, -90f * inst.Frame.Turn, 0);
                SetGates(mat, inst);
                bool off = maze != null && !maze.Contains(inst.Frame.Tile);
                MaterialPool.SetTint(mat, off ? AtlasPalette.Linear(AtlasPalette.Void) : WorldRegionTint.Linear(Regions.RegionOf(inst.Frame.Tile)));
                if (maze != null && ducks < MaxDuckViews && inst.Frame.Tile.Equals(maze.DuckTile))
                    PlaceDuck(ducks++, c, inst);
            }
            for (int i = n; i < objects.Length; i++)
                if (objects[i].activeSelf) objects[i].SetActive(false);
            if (duckObjects != null)
                for (int d = ducks; d < MaxDuckViews; d++)
                    if (duckObjects[d].activeSelf) duckObjects[d].SetActive(false);
            ActiveInstanceCount = n;
            DuckViewCount = ducks;
        }

        void SetGates(Material mat, in CellInstance inst)
        {
            MaterialPool.ClearGates(mat);
            for (int g = 0; g < inst.GateCount; g++)
            {
                double4 gate = inst.Gate(g);
                Vector3 w = root.TransformPoint((float)gate.x, 0, (float)gate.y);
                MaterialPool.SetGate(mat, g, new float2(w.x, w.z), (float)gate.z);
            }
        }

        void PlaceDuck(int d, double2 centre, in CellInstance inst)
        {
            if (duckObjects == null) MakeDucks();
            Material mat = duckMaterials[d];
            if (mat == null)
            {
                mat = duckMaterials[d] = Rent();
                duckObjects[d].GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
            if (!duckObjects[d].activeSelf) duckObjects[d].SetActive(true);
            duckTurn[d] = inst.Frame.Turn;
            Transform tr = duckTransforms[d];
            tr.localPosition = new Vector3((float)centre.x, duckY, (float)centre.y);
            tr.localRotation = Quaternion.Euler(0, -90f * inst.Frame.Turn + duckSpin, 0);
            tr.localScale = new Vector3(duckScale, duckScale, duckScale);
            SetGates(mat, inst);
        }

        void MakeDucks()
        {
            duckMesh = CreateDuckMesh(kit);
            duckObjects = new GameObject[MaxDuckViews];
            duckTransforms = new Transform[MaxDuckViews];
            duckMaterials = new Material[MaxDuckViews];
            for (int d = 0; d < MaxDuckViews; d++)
            {
                duckObjects[d] = MakeObject("Duck" + d, duckMesh, out duckTransforms[d], out _);
                duckObjects[d].SetActive(false);
            }
        }

        // The duck as a Mesh (DuckMesh). A kit whose piece is refused is reported once here and the procedural duck is used.
        static Mesh CreateDuckMesh(KitSet kit)
        {
            var data = new AtlasMeshBuilder(1536, 7680);
            if (!DuckMesh.Build(kit, data, out string error) && error != null)
                Debug.LogWarning("Atlas: kit piece '" + DuckMesh.KitPiece + "' missing or invalid; using the built-in duck. (" + error + ")");
            return AtlasMesh.Create(data, "Duck");
        }

        /// <summary>
        /// Per frame while a duck is drawn: turns, bobs and, after the find, pops the ducks of the last Apply. Transforms only.
        /// The turn is the same in the tile's frame in every view, so the copies round a post agree.
        /// </summary>
        public void Animate(double time, bool found, double foundTime)
        {
            duckSpin = DuckSpin(time, found, foundTime);
            duckY = DuckBob(time);
            duckScale = DuckScale(time, found, foundTime);
            var scale = new Vector3(duckScale, duckScale, duckScale);
            for (int d = 0; d < DuckViewCount; d++)
            {
                Transform tr = duckTransforms[d];
                Vector3 p = tr.localPosition;
                p.y = duckY;
                tr.localPosition = p;
                tr.localRotation = Quaternion.Euler(0, -90f * duckTurn[d] + duckSpin, 0);
                tr.localScale = scale;
            }
        }

        /// <summary>The cell mesh of a kerb mask (0..15): kerbs on the mesh edges whose bit is set. Builds the kerbed ones if needed.</summary>
        public Mesh CellMesh(int kerbMask)
        {
            int mask = kerbMask & 0xF;
            if (mask != 0) BuildKerbs();
            return cellMeshes[mask];
        }
        /// <summary>The mesh instance i draws now.</summary>
        public Mesh InstanceMesh(int i) => filters[i].sharedMesh;
        /// <summary>Duck view d (0..MaxDuckViews - 1), or null before the first duck is drawn.</summary>
        public Transform DuckTransform(int d) => duckTransforms?[d];
        public Material DuckMaterial(int d) => duckMaterials?[d];
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
            if (duckObjects != null)
                for (int d = 0; d < MaxDuckViews; d++)
                {
                    Kill(duckObjects[d]);
                    if (duckMaterials[d] != null) pool.Return(duckMaterials[d]);
                    duckMaterials[d] = null;
                }
            for (int m = 0; m < CellMeshCount; m++) Kill(cellMeshes[m]);
            Kill(postsMesh); Kill(duckMesh);
            MaterialsRented = 0; ActiveInstanceCount = 0; DuckViewCount = 0;
        }

        static void Kill(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
