using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Telemetry;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Spaces.Glued
{
    /// <summary>
    /// S1 Glued (Master Plan tasks P1-A09 and P1-A15): the universal cover of a torus, a Klein bottle or a
    /// 3-torus, drawn fixed to the floor. The walker simply walks through the copies of the cell and meets a
    /// copy of themself in each of them. The room is drawn once: its walls sink to kerbs and its furniture
    /// stands where it really is. Registered: the root is the identity of the room frame.
    /// </summary>
    /// <remarks>
    /// A thin adapter. The lattice maths is in <see cref="GluedLattice"/>, the meshes in
    /// <see cref="GluedCellMesh"/> and <see cref="GluedAvatarMesh"/>, the accounting in <see cref="GluedBudget"/>.
    /// Every copy is a child with its own transform that shares one mesh and one pooled material; a mirrored
    /// Klein row is a child with a scale of -1 along X, for which Unity itself flips the winding.
    /// </remarks>
    public sealed class GluedSpace : MonoBehaviour, ISpaceModule, ISpaceStateReader
    {
        public const string ParamMode = "glued.mode", ParamCopies = "glued.copies", ParamAvatar = "glued.avatar";

        /// <summary>Strength of the floor grid and of the cell outline, 0..1.</summary>
        const float GridStrength = 0.4f, OutlineStrength = 0.9f;

        GluedLattice _lattice;
        bool _standing;
        bool _built;
        bool _entered;

        ParamEntry _mode, _copies, _avatar;
        int _remoteMode = -1, _remoteCopies = -1;
        int _shownMode = -1, _shownCopies = -1;
        bool _shownAvatars;
        GluedTierFollower _tier;

        readonly GluedCell[] _cells = new GluedCell[GluedRules.MaxCells];
        int _count;
        GluedCell _walker;
        bool _hasWalker;
        int _crossings;
        float _heading;

        MaterialPool _pool;
        IRoomContent _room;
        ISpaceHost _host;
        Material _cellMaterial, _plainMaterial;
        Mesh _cellMesh, _avatarMesh, _promptMesh;
        GameObject _content, _prompt;
        Transform _promptTransform;
        GameObject[] _cellObjects, _avatarObjects;
        Transform[] _cellTransforms, _avatarTransforms;

        /// <summary>The factory given to SpaceRegistry: a new object named like the scene.</summary>
        public static ISpaceModule Create()
        {
            return new GameObject(SpaceIds.SceneName(SpaceId.Glued)).AddComponent<GluedSpace>();
        }

        /// <summary>The parameters of group "glued" (lane A contract, section 14.1). Safe to call again.</summary>
        public static void DeclareParams(ParamRegistry registry)
        {
            string group = SpaceIds.ParamGroup(SpaceId.Glued);
            registry.DeclareEnum(ParamMode, group, GluedRules.LayoutNames, 0, null, true);
            registry.DeclareInt(ParamCopies, group, GluedRules.MinCopies, GluedRules.MaxCopies, GluedRules.DefaultCopies, null, true);
            registry.DeclareBool(ParamAvatar, group, true);
        }

        public SpaceId Id { get { return SpaceId.Glued; } }
        public bool Registered { get { return true; } }

        /// <summary>The lattice in force.</summary>
        public GluedLattice Lattice { get { return _lattice; } }
        /// <summary>The cell the walker's head is in.</summary>
        public GluedCell WalkerCell { get { return _walker; } }
        /// <summary>Cell edges crossed since Enter.</summary>
        public int Crossings { get { return _crossings; } }
        /// <summary>The room is too small to walk: the space runs in place and never prompts.</summary>
        public bool Standing { get { return _standing; } }

        public void Configure(RoomModel room, ParamRegistry registry)
        {
            DeclareParams(registry);
            // The handles are kept and polled in Tick: no callback outlives this module.
            _mode = registry.Find(ParamMode);
            _copies = registry.Find(ParamCopies);
            _avatar = registry.Find(ParamAvatar);

            _standing = room.Standing;
            _lattice = new GluedLattice(room, CurrentLayout());
            AtlasServices.TryGet(out _room);
            AtlasServices.TryGet(out _host);
            if (!AtlasServices.TryGet(out _pool)) return;     // no look in this build: nothing to draw with

            BuildContent();
            _built = true;
        }

        public void Enter(in WalkerPose start)
        {
            _entered = true;
            _hasWalker = false;
            _crossings = 0;
            _heading = 0f;
            _tier = default;

            if (_room != null && _room.IsBuilt)
            {
                // The room is not repeated: its walls sink to kerbs, its furniture stays, and the cells bring the floor.
                _room.SetWallMode(WallMode.Kerbs);
                _room.FloorVisible = false;
                _room.ObjectsVisible = true;
            }
            // Until the first Tick brings a head pose, the lattice is laid round the middle of the room.
            _walker = _lattice.CellOf(float3.zero);
            _shownMode = -1;
            Refresh(false);
            if (_content != null) _content.SetActive(true);
        }

        public void ReadState(in StateMessage state)
        {
            if (state.SpaceId != (byte)SpaceId.Glued) return;
            // The Station draws what the phone draws, whatever its own registry holds.
            _remoteMode = math.clamp((int)math.round(state.P3), 0, GluedRules.LayoutNames.Length - 1);
            _remoteCopies = math.clamp((int)math.round(state.P4), GluedRules.MinCopies, GluedRules.MaxCopies);
        }

        public void Tick(in FrameContext frame)
        {
            if (!_entered) return;
            if (_remoteCopies < 0 && _copies != null)
            {
                int wanted = _tier.Step(frame.Tier, _copies.Int);
                if (wanted != _copies.Int) _copies.Set(wanted);
            }

            bool changed = Refresh(true);

            GluedCell cell = _lattice.CellOf(frame.HeadPosition);
            if (!_hasWalker || !cell.Equals(_walker) || changed)
            {
                // The lattice is fixed to the floor, so re-centring only moves cells that lie in the fog.
                if (_hasWalker && !changed && !cell.Equals(_walker)) _crossings++;
                _walker = cell;
                _hasWalker = true;
                Layout();
            }

            _heading = GluedAvatarPose.Heading(frame.HeadRotation, _heading);
            if (!_built) return;
            if (_shownAvatars) PoseAvatars(frame.HeadPosition);
            PlacePrompt(frame.HeadPosition);
        }

        public void Exit()
        {
            _entered = false;
            if (_content != null) _content.SetActive(false);
            if (_pool != null)
            {
                if (_cellMaterial != null) _pool.Return(_cellMaterial);
                if (_plainMaterial != null) _pool.Return(_plainMaterial);
            }
            _cellMaterial = null;
            _plainMaterial = null;
            DestroyMesh(ref _cellMesh);
            DestroyMesh(ref _avatarMesh);
            DestroyMesh(ref _promptMesh);
            _built = false;
        }

        public void WriteState(ref StateMessage state)
        {
            state.SetTile(_walker.Col, _walker.Row, 0, 0, 0, 0);
            state.P3 = (float)_lattice.Layout;
            state.P4 = GluedRules.CopiesPerSide(_lattice.Layout, CurrentCopies());
            state.P5 = (float)_lattice.SizeX;
            state.P6 = (float)_lattice.SizeZ;
            state.P7 = 0f;
        }

        // ---- Parameters ----------------------------------------------------------------------------------------

        GluedLayout CurrentLayout()
        {
            return GluedRules.LayoutOf(_remoteMode >= 0 ? _remoteMode : _mode != null ? _mode.Int : 0);
        }

        int CurrentCopies()
        {
            return _remoteCopies >= 0 ? _remoteCopies : _copies != null ? _copies.Int : GluedRules.DefaultCopies;
        }

        // Takes the parameters in force. True when the layout or the copy count changed, which regenerates the
        // lattice: the world version moves on so the Station redraws too.
        bool Refresh(bool announce)
        {
            int mode = (int)CurrentLayout();
            int copies = GluedRules.CopiesPerSide((GluedLayout)mode, CurrentCopies());
            bool avatars = _avatar == null || _avatar.Bool;
            bool changed = mode != _shownMode || copies != _shownCopies;
            if (!changed && avatars == _shownAvatars) return false;

            _shownMode = mode;
            _shownCopies = copies;
            _shownAvatars = avatars;
            if (changed)
            {
                _lattice = new GluedLattice(in _lattice, (GluedLayout)mode);
                GluedRules.Fog(in _lattice, copies, out float start, out float end);
                AtlasLook.SetFog(start, end);
                if (announce && _host != null) _host.BumpWorldVersion();
            }
            if (!announce || !changed) Layout();
            return changed;
        }

        // ---- Content -------------------------------------------------------------------------------------------

        void BuildContent()
        {
            float sizeX = (float)_lattice.SizeX, sizeZ = (float)_lattice.SizeZ;
            // The pieces modelled in Blender; a piece that is missing or refused keeps the built-in mesh.
            KitSet kit = KitSet.Load(ReadKitResource);
            MeshData landmark = KitPiece(kit, "landmark"), avatar = KitPiece(kit, "avatar");
            var data = new AtlasMeshBuilder(1024, 4096);
            GluedCellMesh.Build(sizeX, sizeZ, data, landmark);
            _cellMesh = AtlasMesh.Create(data, "Glued Cell");
            data.Clear();
            GluedAvatarMesh.Build(data, avatar);
            _avatarMesh = AtlasMesh.Create(data, "Glued Avatar");
            data.Clear();
            GluedPromptMesh.Build(data);
            _promptMesh = AtlasMesh.Create(data, "Glued Prompt");

            // One instance for every cell and one for everything without lines: two set-passes for the lattice.
            _cellMaterial = _pool.Rent();
            MaterialPool.SetLines(_cellMaterial, GridStrength, OutlineStrength,
                new float2(GluedCellMesh.GridDivisions(sizeX), GluedCellMesh.GridDivisions(sizeZ)));
            _plainMaterial = _pool.Rent();

            _content = new GameObject("Lattice");
            _content.transform.SetParent(transform, false);
            _content.SetActive(false);

            int max = GluedRules.MaxCells;
            _cellObjects = new GameObject[max];
            _avatarObjects = new GameObject[max];
            _cellTransforms = new Transform[max];
            _avatarTransforms = new Transform[max];
            for (int i = 0; i < max; i++)
            {
                _cellObjects[i] = NewRenderer("Cell", _cellMesh, _cellMaterial, out _cellTransforms[i]);
                _avatarObjects[i] = NewRenderer("Avatar", _avatarMesh, _plainMaterial, out _avatarTransforms[i]);
            }
            _prompt = NewRenderer("Prompt", _promptMesh, _plainMaterial, out _promptTransform);
        }

        static MeshData KitPiece(KitSet kit, string name)
        {
            var mesh = new MeshData(512, 2048);
            if (kit.TryGet(name, mesh, out string error)) return mesh;
            Debug.LogWarning("Atlas: kit piece '" + name + "' missing or invalid; using the built-in one. (" + error + ")");
            return null;
        }

        static byte[] ReadKitResource(string name)
        {
            var asset = Resources.Load<TextAsset>(KitSet.ResourceFolder + "/" + name);
            if (asset == null) return null;
            byte[] bytes = asset.bytes;
            Resources.UnloadAsset(asset);
            return bytes;
        }

        GameObject NewRenderer(string label, Mesh mesh, Material material, out Transform child)
        {
            var go = new GameObject(label);
            child = go.transform;
            child.SetParent(_content.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            go.SetActive(false);
            return go;
        }

        // Puts every copy where the lattice says: only called when the walker's cell or a parameter changes.
        void Layout()
        {
            _count = _lattice.FillCopies(_walker, _shownCopies, _cells);
            if (!_built) return;
            for (int i = 0; i < _cellObjects.Length; i++)
            {
                bool drawn = i < _count;
                SetActive(_cellObjects[i], drawn);
                bool avatar = drawn && _shownAvatars && !_cells[i].Equals(_walker);
                SetActive(_avatarObjects[i], avatar);
                if (!drawn) continue;

                GluedCell cell = _cells[i];
                _cellTransforms[i].localPosition = (float3)_lattice.CellCentre(cell);
                _cellTransforms[i].localScale = new Vector3(_lattice.IsMirrored(cell) ? -1f : 1f, 1f, 1f);
                if (avatar)
                {
                    bool mirrored = _lattice.Between(_walker, cell).Mirror;
                    _avatarTransforms[i].localScale = new Vector3(mirrored ? -1f : 1f, 1f, 1f);
                }
            }
        }

        void PoseAvatars(float3 head)
        {
            for (int i = 0; i < _count; i++)
            {
                GluedCell cell = _cells[i];
                if (cell.Equals(_walker)) continue;
                GluedAvatarPose.InCell(in _lattice, _walker, cell, head, _heading,
                                       out float3 position, out float heading, out bool _);
                Transform avatar = _avatarTransforms[i];
                avatar.localPosition = position;
                avatar.localRotation = quaternion.RotateY(heading);
            }
        }

        void PlacePrompt(float3 head)
        {
            float2 position = float2.zero;
            float turn = 0f;
            bool shown = GluedPrompt.Shown(in _lattice, _standing, _crossings)
                         && GluedPrompt.Place(in _lattice, head.xz, out position, out turn);
            SetActive(_prompt, shown);
            if (!shown) return;
            _promptTransform.localPosition = new Vector3(position.x, 0f, position.y);
            _promptTransform.localRotation = quaternion.RotateY(turn);
        }

        static void SetActive(GameObject go, bool active)
        {
            if (go.activeSelf != active) go.SetActive(active);
        }

        static void DestroyMesh(ref Mesh mesh)
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
            mesh = null;
        }
    }
}
